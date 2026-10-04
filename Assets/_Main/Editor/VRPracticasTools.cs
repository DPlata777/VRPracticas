using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRPracticas.EditorTools
{
    /// <summary>
    /// Menú "VR Practicas":
    /// - Agrega HandGrab o TouchHandGrab (Meta Interaction SDK) con la misma estructura que los cubos
    ///   de ejemplo de los Building Blocks (VR / Interactables / Kinematic y Physic - Touch).
    /// - Ordena las escenas: la de trabajo estaba en _Recovery (carpeta de recuperación tras un crash).
    /// </summary>
    public static class VRPracticasTools
    {
        const string CupcakePrefix = "PW_cupcake";
        const string HandGrabChildName = "HandGrabInteraction";
        const string TouchHandGrabChildName = "TouchHandGrabInteraction";

        const string MainScenesDir = "Assets/_Main/Scenes";
        const string MainScenePath = MainScenesDir + "/EscenaPrincipal.unity";
        const string MainSceneDataDir = MainScenesDir + "/EscenaPrincipal";
        const string BackupDir = MainScenesDir + "/Respaldo";
        const string RecoveredScenePath = "Assets/_Recovery/0.unity";
        const string OldScenePath = "Assets/_Main/DecorativeScene 1.unity";
        // NavMesh horneado: lo usan tanto la escena principal como la vieja (referencia por GUID, sobrevive al moverlo).
        const string OldSceneDataDir = "Assets/_Main/DecorativeScene 1";
        const string EmptyScenePath = "Assets/Scenes/SampleScene.unity";

        // ------------------------------------------------------------------ Agarre

        [MenuItem("VR Practicas/1. Agarre en 2 cupcakes", priority = 0)]
        static void SetupTwoCupcakes()
        {
            var scene = SceneManager.GetActiveScene();
            var candidates = AllInScene<Transform>(scene)
                .Where(t => t.name.StartsWith(CupcakePrefix, System.StringComparison.OrdinalIgnoreCase)
                            && t.GetComponent<MeshFilter>()
                            && !t.GetComponent<Oculus.Interaction.Grabbable>())
                .ToList();

            if (candidates.Count < 2)
            {
                EditorUtility.DisplayDialog("VR Practicas",
                    "No encontré 2 cupcakes sin configurar en la escena abierta.\n\n" +
                    "Si ya los configuraste, no hace falta repetirlo. Para otros objetos usa " +
                    "\"Agregar HandGrab / TouchHandGrab a lo seleccionado\".", "OK");
                return;
            }

            // Se eligen los cupcakes más cercanos a los cubos de ejemplo del profe, que están al alcance del jugador.
            var handGrabRef = ReferencePoint<Oculus.Interaction.HandGrab.HandGrabInteractable>(scene);
            var touchRef = ReferencePoint<Oculus.Interaction.TouchHandGrabInteractable>(scene);

            var handGrabCupcake = Closest(candidates, handGrabRef);
            candidates.Remove(handGrabCupcake);
            var touchCupcake = Closest(candidates, touchRef);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Agarre en 2 cupcakes");
            Configure(handGrabCupcake.gameObject, touch: false);
            Configure(touchCupcake.gameObject, touch: true);
            Undo.CollapseUndoOperations(group);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.objects = new Object[] { handGrabCupcake.gameObject, touchCupcake.gameObject };
            EditorGUIUtility.PingObject(handGrabCupcake.gameObject);

            EditorUtility.DisplayDialog("VR Practicas",
                $"HandGrab:       {handGrabCupcake.name}\n" +
                $"TouchHandGrab:  {touchCupcake.name}\n\n" +
                "Quedaron seleccionados en la Jerarquía. Guarda la escena (Ctrl+S) y pruébalos en Play.\n" +
                "Ctrl+Z deshace todo de una vez.", "OK");
        }

        [MenuItem("VR Practicas/Agregar HandGrab a lo seleccionado", priority = 20)]
        static void HandGrabOnSelection() => ApplyToSelection(touch: false);

        [MenuItem("VR Practicas/Agregar TouchHandGrab a lo seleccionado", priority = 21)]
        static void TouchHandGrabOnSelection() => ApplyToSelection(touch: true);

        [MenuItem("VR Practicas/Agregar HandGrab a lo seleccionado", true)]
        [MenuItem("VR Practicas/Agregar TouchHandGrab a lo seleccionado", true)]
        static bool HasSceneSelection() => Selection.gameObjects.Any(go => go.scene.IsValid());

        static void ApplyToSelection(bool touch)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(touch ? "Agregar TouchHandGrab" : "Agregar HandGrab");
            foreach (var go in Selection.gameObjects.Where(go => go.scene.IsValid()))
            {
                if (Configure(go, touch))
                    EditorSceneManager.MarkSceneDirty(go.scene);
            }
            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Misma estructura que el Building Block:
        ///   objeto -> Collider + Rigidbody + Grabbable
        ///   hijo   -> HandGrabInteractable + GrabInteractable  (o TouchHandGrabInteractable)
        /// HandGrab: Rigidbody cinemático sin gravedad y collider trigger (queda donde lo sueltas).
        /// TouchHandGrab: Rigidbody con gravedad y collider sólido (cae y choca con la mesa).
        /// </summary>
        static bool Configure(GameObject target, bool touch)
        {
            if (target.GetComponent<Oculus.Interaction.Grabbable>())
            {
                Debug.LogWarning($"[VR Practicas] {target.name} ya tiene Grabbable; no lo modifico.", target);
                return false;
            }

            // Un objeto estático se "hornea" y no se movería al agarrarlo.
            if (GameObjectUtility.GetStaticEditorFlags(target) != 0)
            {
                Undo.RecordObject(target, "Quitar Static");
                GameObjectUtility.SetStaticEditorFlags(target, 0);
            }

            var collider = PrepareCollider(target, isTrigger: !touch);

            var rigidbody = Undo.AddComponent<Rigidbody>(target);
            rigidbody.isKinematic = !touch;
            rigidbody.useGravity = touch;

            var grabbable = Undo.AddComponent<Oculus.Interaction.Grabbable>(target);
            SetRef(grabbable, "_targetTransform", target.transform);
            SetRef(grabbable, "_rigidbody", rigidbody);

            var child = new GameObject(touch ? TouchHandGrabChildName : HandGrabChildName);
            child.transform.SetParent(target.transform, false);
            Undo.RegisterCreatedObjectUndo(child, "Crear interacción");

            if (touch)
            {
                var touchGrab = Undo.AddComponent<Oculus.Interaction.TouchHandGrabInteractable>(child);
                SetRef(touchGrab, "_pointableElement", grabbable);
                SetRef(touchGrab, "_boundsCollider", collider);
                SetSingleItemList(touchGrab, "_colliders", collider);
            }
            else
            {
                var handGrab = Undo.AddComponent<Oculus.Interaction.HandGrab.HandGrabInteractable>(child);
                SetRef(handGrab, "_pointableElement", grabbable);
                SetRef(handGrab, "_rigidbody", rigidbody);

                // Igual que el Building Block: además permite agarrarlo con los controles.
                var controllerGrab = Undo.AddComponent<Oculus.Interaction.GrabInteractable>(child);
                SetRef(controllerGrab, "_pointableElement", grabbable);
                SetRef(controllerGrab, "_rigidbody", rigidbody);
            }

            Debug.Log($"[VR Practicas] {(touch ? "TouchHandGrab" : "HandGrab")} agregado a {target.name}.", target);
            return true;
        }

        /// <summary>
        /// El SDK toma todos los colliders del Rigidbody y les pide ClosestPoint, que solo funciona con colliders
        /// convexos; y un Rigidbody con física tampoco acepta un MeshCollider no convexo. Por eso se marca Convex.
        /// </summary>
        static Collider PrepareCollider(GameObject target, bool isTrigger)
        {
            foreach (var meshCollider in target.GetComponentsInChildren<MeshCollider>(true))
            {
                if (meshCollider.convex)
                    continue;
                Undo.RecordObject(meshCollider, "Mesh Collider convexo");
                meshCollider.convex = true;
            }

            var collider = target.GetComponent<Collider>();
            if (!collider)
            {
                var box = Undo.AddComponent<BoxCollider>(target);
                var meshFilter = target.GetComponent<MeshFilter>();
                if (meshFilter && meshFilter.sharedMesh)
                {
                    box.center = meshFilter.sharedMesh.bounds.center;
                    box.size = meshFilter.sharedMesh.bounds.size;
                }
                collider = box;
            }

            Undo.RecordObject(collider, "Collider del agarre");
            collider.isTrigger = isTrigger;
            return collider;
        }

        // ------------------------------------------------------------------ Organización

        [MenuItem("VR Practicas/2. Organizar escenas y carpetas", priority = 1)]
        static void OrganizeProject()
        {
            var steps = new StringBuilder();
            bool moveMain = Exists(RecoveredScenePath) && !Exists(MainScenePath);
            bool moveData = Exists(OldSceneDataDir) && !Exists(MainSceneDataDir);
            bool moveOld = Exists(OldScenePath);
            bool removeEmpty = Exists(EmptyScenePath);
            bool removeTutorial = Exists("Assets/TutorialInfo") || Exists("Assets/Readme.asset");

            if (moveMain) steps.AppendLine($"• Mover la escena de trabajo:\n   {RecoveredScenePath}\n   → {MainScenePath}");
            if (moveData) steps.AppendLine($"• Mover el NavMesh horneado a {MainSceneDataDir}/");
            if (moveOld) steps.AppendLine($"• Mover la versión anterior (DecorativeScene 1) a {BackupDir}/");
            if (removeEmpty) steps.AppendLine($"• Mandar a la papelera la escena vacía {EmptyScenePath}");
            if (removeTutorial) steps.AppendLine("• Mandar a la papelera el tutorial de la plantilla URP (TutorialInfo, Readme)");
            steps.AppendLine("• Dejar solo EscenaPrincipal en Build Settings");

            if (!EditorUtility.DisplayDialog("Organizar proyecto",
                    steps + "\nAntes se guardan las escenas abiertas (con tus cambios actuales).", "Organizar", "Cancelar"))
                return;

            if (!EditorSceneManager.SaveOpenScenes())
            {
                EditorUtility.DisplayDialog("Organizar proyecto", "No se pudo guardar la escena abierta; no moví nada.", "OK");
                return;
            }

            EnsureFolder(MainScenesDir);
            if (moveMain) Move(RecoveredScenePath, MainScenePath);
            if (moveData) Move(OldSceneDataDir, MainSceneDataDir);
            if (moveOld)
            {
                EnsureFolder(BackupDir);
                Move(OldScenePath, BackupDir + "/DecorativeScene 1.unity");
            }
            if (removeEmpty) Trash(EmptyScenePath);
            Trash("Assets/TutorialInfo");
            Trash("Assets/Readme.asset");
            TrashFolderIfEmpty("Assets/Scenes");
            TrashFolderIfEmpty("Assets/_Recovery");

            if (Exists(MainScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Organizar proyecto",
                $"Listo. La escena de trabajo ahora es {MainScenePath} y es la única en Build Settings.", "OK");
        }

        // ------------------------------------------------------------------ Utilidades

        static List<T> AllInScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToList();

        static Vector3 ReferencePoint<T>(Scene scene) where T : Component
        {
            var example = AllInScene<T>(scene).FirstOrDefault(c => !IsCupcake(c.transform));
            if (example)
                return example.transform.position;
            var camera = AllInScene<Camera>(scene).FirstOrDefault();
            return camera ? camera.transform.position : Vector3.zero;
        }

        static bool IsCupcake(Transform t)
        {
            for (; t; t = t.parent)
            {
                if (t.name.StartsWith(CupcakePrefix, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static Transform Closest(List<Transform> candidates, Vector3 point) =>
            candidates.OrderBy(t => (t.position - point).sqrMagnitude).First();

        static void SetRef(Object component, string field, Object value)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[VR Practicas] {component.GetType().Name} no tiene el campo {field}; ¿cambió la versión del Meta SDK?", component);
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }

        static void SetSingleItemList(Object component, string field, Object value)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(field);
            if (property == null || !property.isArray)
            {
                Debug.LogError($"[VR Practicas] {component.GetType().Name} no tiene la lista {field}; ¿cambió la versión del Meta SDK?", component);
                return;
            }
            property.arraySize = 1;
            property.GetArrayElementAtIndex(0).objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }

        static bool Exists(string path) => AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path);

        static void Move(string from, string to)
        {
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"[VR Practicas] No pude mover {from} → {to}: {error}");
        }

        static void Trash(string path)
        {
            if (Exists(path))
                AssetDatabase.MoveAssetToTrash(path);
        }

        static void TrashFolderIfEmpty(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder) && AssetDatabase.FindAssets("", new[] { folder }).Length == 0)
                AssetDatabase.MoveAssetToTrash(folder);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}

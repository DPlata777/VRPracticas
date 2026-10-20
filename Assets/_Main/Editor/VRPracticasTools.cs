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

        // ------------------------------------------------------------------ Cajón

        const string CabinetName = "Cabinet";
        const string DrawerName = "C Drawer 1";

        /// <summary>
        /// Igual que el muñeco de nieve (Building Block "Physic - Touch": Rigidbody con gravedad + Grabbable +
        /// TouchHandGrabInteractable), pero el cajón solo se desliza hacia afuera: un OneGrabTranslateTransformer
        /// limita el movimiento mientras se agarra y las restricciones del Rigidbody lo mantienen en su riel al soltarlo.
        /// </summary>
        [MenuItem("VR Practicas/3. Cajón que se abre (Physic - Touch)", priority = 2)]
        static void SetupDrawer()
        {
            var scene = SceneManager.GetActiveScene();
            // Si hay otro cajón del mueble seleccionado se usa ese; si no, el de arriba.
            var selected = Selection.activeGameObject ? Selection.activeGameObject.transform : null;
            var drawer = selected && selected.gameObject.scene.IsValid() && IsInCabinet(selected)
                ? selected
                : AllInScene<Transform>(scene).FirstOrDefault(t => t.name == DrawerName && IsInCabinet(t));
            if (!drawer)
            {
                EditorUtility.DisplayDialog("VR Practicas",
                    $"No encontré el cajón \"{DrawerName}\" dentro de \"{CabinetName}\".\n\n" +
                    "Selecciona en la Jerarquía el cajón que quieres que se abra y vuelve a usar este menú.", "OK");
                return;
            }

            if (!TryGetPullAxis(drawer, out int axis, out float sign, out float depth))
            {
                EditorUtility.DisplayDialog("VR Practicas",
                    $"No pude calcular hacia dónde abre {drawer.name} (le falta el MeshFilter a él o al mueble).", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Cajón que se abre");

            if (!Configure(drawer.gameObject, touch: true))
            {
                Undo.CollapseUndoOperations(group);
                return;
            }

            var grabbable = drawer.GetComponent<Oculus.Interaction.Grabbable>();
            var rigidbody = drawer.GetComponent<Rigidbody>();

            // Solo se mueve sobre el eje del riel, desde cerrado hasta el 70 % de su profundidad.
            float travel = depth * 0.7f;
            var translate = Undo.AddComponent<Oculus.Interaction.OneGrabTranslateTransformer>(drawer.gameObject);
            var serialized = new SerializedObject(translate);
            serialized.FindProperty("_constraints.ConstraintsAreRelative").boolValue = true;
            string[] axes = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                float min = 0f, max = 0f;
                if (i == axis)
                {
                    min = sign > 0 ? 0f : -travel;
                    max = sign > 0 ? travel : 0f;
                }
                SetConstraint(serialized, "Min" + axes[i], min);
                SetConstraint(serialized, "Max" + axes[i], max);
            }
            serialized.ApplyModifiedProperties();
            SetRef(grabbable, "_oneGrabTransformer", translate);

            // Si se "lanzara" al soltarlo, saldría disparado por el riel.
            var grabbableSerialized = new SerializedObject(grabbable);
            grabbableSerialized.FindProperty("_throwWhenUnselected").boolValue = false;
            grabbableSerialized.ApplyModifiedProperties();

            // Las restricciones del Rigidbody son en ejes del mundo: sirven si el mueble está alineado con ellos.
            Vector3 railWorld = drawer.parent.TransformDirection(AxisVector(axis)).normalized;
            int worldAxis = Enumerable.Range(0, 3).OrderByDescending(i => Mathf.Abs(railWorld[i])).First();
            Undo.RecordObject(rigidbody, "Riel del cajón");
            if (Mathf.Abs(railWorld[worldAxis]) > 0.99f)
            {
                var freeze = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePosition;
                freeze &= ~(RigidbodyConstraints)(1 << (worldAxis + 1)); // FreezePositionX = 2, Y = 4, Z = 8
                rigidbody.constraints = freeze;
            }
            else
            {
                // Mueble girado en diagonal: se deja cinemático para que no se caiga al soltarlo.
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(drawer.gameObject.scene);
            Selection.activeGameObject = drawer.gameObject;
            EditorGUIUtility.PingObject(drawer.gameObject);

            EditorUtility.DisplayDialog("VR Practicas",
                $"{drawer.name} ya se puede abrir con la mano (Touch Hand Grab, igual que el muñeco de nieve).\n" +
                $"Abre {travel:0.00} m hacia {(sign > 0 ? "+" : "-")}{axes[axis]} del mueble.\n\n" +
                "Guarda la escena (Ctrl+S) y pruébalo en Play. Ctrl+Z deshace todo de una vez.", "OK");
        }

        /// <summary>
        /// El frente del cajón queda al ras del mueble y el fondo queda adentro: el eje horizontal donde los
        /// huecos a cada lado son más distintos es el riel, y el lado con menos hueco es hacia donde abre.
        /// </summary>
        static bool TryGetPullAxis(Transform drawer, out int axis, out float sign, out float depth)
        {
            axis = 2; sign = 1f; depth = 0f;
            var drawerMesh = drawer.GetComponent<MeshFilter>();
            var cabinetMesh = drawer.parent.GetComponent<MeshFilter>();
            if (!drawerMesh || !drawerMesh.sharedMesh || !cabinetMesh || !cabinetMesh.sharedMesh)
                return false;

            Bounds cabinet = cabinetMesh.sharedMesh.bounds;
            Bounds local = drawerMesh.sharedMesh.bounds;
            var inCabinet = new Bounds(drawer.parent.InverseTransformPoint(drawer.TransformPoint(local.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                inCabinet.Encapsulate(drawer.parent.InverseTransformPoint(drawer.TransformPoint(corner)));
            }

            float best = -1f;
            foreach (int i in new[] { 0, 2 })
            {
                float gapPositive = cabinet.max[i] - inCabinet.max[i];
                float gapNegative = inCabinet.min[i] - cabinet.min[i];
                float asymmetry = Mathf.Abs(gapPositive - gapNegative);
                if (asymmetry <= best)
                    continue;
                best = asymmetry;
                axis = i;
                sign = gapPositive < gapNegative ? 1f : -1f;
                depth = inCabinet.size[i];
            }
            return depth > 0f;
        }

        static bool IsInCabinet(Transform t) => t.parent && t.parent.name.Trim() == CabinetName;

        static Vector3 AxisVector(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        static void SetConstraint(SerializedObject serialized, string name, float value)
        {
            serialized.FindProperty($"_constraints.{name}.Constrain").boolValue = true;
            serialized.FindProperty($"_constraints.{name}.Value").floatValue = value;
        }

        // ------------------------------------------------------------------ Organización

        [MenuItem("VR Practicas/2. Organizar escenas y carpetas", priority = 1)]
        static void OrganizeProject()
        {
            var steps = new StringBuilder();
            bool moveMain = Exists(RecoveredScenePath) && !Exists(MainScenePath);
            bool moveData = Exists(OldSceneDataDir) && !Exists(MainSceneDataDir);
            bool removeOld = Exists(OldScenePath);
            bool removeEmpty = Exists(EmptyScenePath);
            bool removeTutorial = Exists("Assets/TutorialInfo") || Exists("Assets/Readme.asset");

            if (moveMain) steps.AppendLine($"• Mover la escena de trabajo:\n   {RecoveredScenePath}\n   → {MainScenePath}");
            if (moveData) steps.AppendLine($"• Mover el NavMesh horneado a {MainSceneDataDir}/");
            if (removeOld) steps.AppendLine($"• Eliminar la versión anterior {OldScenePath}");
            if (removeEmpty) steps.AppendLine($"• Eliminar la escena vacía {EmptyScenePath}");
            if (removeTutorial) steps.AppendLine("• Eliminar el tutorial de la plantilla URP (TutorialInfo, Readme)");
            steps.AppendLine("• Dejar solo EscenaPrincipal en Build Settings");
            steps.AppendLine("\n(Lo eliminado va a la papelera de Windows y sigue en el historial de Git.)");

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
            if (removeOld) Trash(OldScenePath);
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

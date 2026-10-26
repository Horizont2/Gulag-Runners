// Stand-ins for UnityEditor, so compile_check can build Assets/_Game/Editor too.
//
// UnityEngine ships reference assemblies on NuGet; UnityEditor does not -- it comes with
// the editor itself. That is why this folder existed only for two packages before, and
// why compile_check's own header used to say editor scripts "cannot break a build".
//
// THAT WAS WRONG, and a blocked editor proved it: one stale field reference in
// HudViewEditor.cs stopped Unity compiling ANYTHING, which stops play mode, which stops
// the game. An editor script cannot break a PLAYER build; it can and does break the
// editor, which is where the game is actually made.
//
// Only what Assets/_Game/Editor actually touches is here -- about seventy members across
// twenty types, extracted from the sources rather than guessed. Anything this project
// does not call is deliberately absent: a stub that invents API surface is a stub that
// will one day let a call through that Unity refuses.
using System;
using UnityEngine;

namespace UnityEditor
{
    public class Editor : ScriptableObject
    {
        public UnityEngine.Object target;
        public UnityEngine.Object[] targets;
        public SerializedObject serializedObject;
        public virtual void OnInspectorGUI() { }
        public void DrawDefaultInspector() { }
        public void Repaint() { }
        public static Editor CreateEditor(UnityEngine.Object obj) => null;
    }

    public class EditorWindow : ScriptableObject
    {
        public string title;
        public Vector2 minSize;
        public void Show() { }
        public void Close() { }
        public void Repaint() { }
        public static new void FocusWindowIfItsOpen<T>() where T : EditorWindow { }
        public static T GetWindow<T>(bool utility, string title) where T : EditorWindow => null;
        public static T GetWindow<T>(string title) where T : EditorWindow => null;
        public static T GetWindow<T>() where T : EditorWindow => null;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string path) { }
        public MenuItemAttribute(string path, bool validate) { }
        public MenuItemAttribute(string path, bool validate, int priority) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CustomEditorAttribute : Attribute
    {
        public CustomEditorAttribute(Type type) { }
        public CustomEditorAttribute(Type type, bool editorForChildClasses) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CanEditMultipleObjectsAttribute : Attribute { }

    public class DefaultAsset : UnityEngine.Object { }

    public static class EditorGUILayout
    {
        public static void LabelField(string label) { }
        public static void LabelField(string label, GUIStyle style) { }
        public static void LabelField(string label, string value) { }
        public static void LabelField(string label, string value, GUIStyle style) { }
        public static void LabelField(GUIContent label, GUIStyle style) { }
        public static void Space() { }
        public static void Space(float pixels) { }
        public static void HelpBox(string message, MessageType type) { }
        public static bool Toggle(string label, bool value) => value;
        public static bool Toggle(GUIContent label, bool value) => value;
        public static int IntField(string label, int value) => value;
        public static int IntField(GUIContent label, int value) => value;
        public static float Slider(string label, float value, float min, float max) => value;
        public static float Slider(GUIContent label, float value, float min, float max) => value;
        public static string TextField(string label, string text) => text;
        public static string TextField(GUIContent label, string text) => text;
        public static Vector2 Vector2Field(string label, Vector2 value) => value;
        public static Enum EnumPopup(string label, Enum selected) => selected;
        public static Enum EnumPopup(GUIContent label, Enum selected) => selected;
        public static UnityEngine.Object ObjectField(string label, UnityEngine.Object obj,
                                                     Type objType, bool allowSceneObjects) => obj;
        public static UnityEngine.Object ObjectField(GUIContent label, UnityEngine.Object obj,
                                                     Type objType, bool allowSceneObjects) => obj;
        public static Vector2 BeginScrollView(Vector2 pos, params GUILayoutOption[] options) => pos;
        public static void EndScrollView() { }

        public class HorizontalScope : IDisposable
        {
            public HorizontalScope(params GUILayoutOption[] options) { }
            public HorizontalScope(GUIStyle style, params GUILayoutOption[] options) { }
            public void Dispose() { }
        }

        public class VerticalScope : IDisposable
        {
            public VerticalScope(params GUILayoutOption[] options) { }
            public VerticalScope(GUIStyle style, params GUILayoutOption[] options) { }
            public void Dispose() { }
        }
    }

    public static class EditorGUI
    {
        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() => false;

        public class DisabledScope : IDisposable
        {
            public DisabledScope(bool disabled) { }
            public void Dispose() { }
        }
    }

    public static class EditorGUIUtility
    {
        public static void PingObject(UnityEngine.Object obj) { }
        public static void PingObject(int instanceID) { }
    }

    public static class EditorStyles
    {
        public static GUIStyle boldLabel => null;
        public static GUIStyle miniLabel => null;
        public static GUIStyle miniBoldLabel => null;
        public static GUIStyle wordWrappedMiniLabel => null;
        public static GUIStyle helpBox => null;
    }

    public enum MessageType { None, Info, Warning, Error }

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target) { }
        public static bool DisplayDialog(string title, string message, string ok) => true;
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => true;
    }

    public static class AssetDatabase
    {
        public static string[] FindAssets(string filter) => new string[0];
        public static string[] FindAssets(string filter, string[] searchInFolders) => new string[0];
        public static string GUIDToAssetPath(string guid) => "";
        public static string GetAssetPath(UnityEngine.Object asset) => "";
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string path) =>
            new UnityEngine.Object[0];
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static string CreateFolder(string parent, string newFolderName) => "";
        public static bool IsValidFolder(string path) => false;
        public static void ImportAsset(string path) { }
        public static void ImportAsset(string path, ImportAssetOptions options) { }
        public static void SaveAssets() { }
        public static void SaveAssetIfDirty(UnityEngine.Object asset) { }
        public static void Refresh() { }
    }

    [Flags]
    public enum ImportAssetOptions { Default = 0, ForceUpdate = 1, ForceSynchronousImport = 8 }

    public static class Undo
    {
        public static T AddComponent<T>(GameObject go) where T : Component => null;
        public static Component AddComponent(GameObject go, Type type) => null;
        public static void RecordObject(UnityEngine.Object o, string name) { }
        public static void RecordObjects(UnityEngine.Object[] o, string name) { }
        public static void RegisterCreatedObjectUndo(UnityEngine.Object o, string name) { }
        public static void DestroyObjectImmediate(UnityEngine.Object o) { }
        public static void SetTransformParent(Transform t, Transform parent, string name) { }
        public static int GetCurrentGroup() => 0;
        public static void CollapseUndoOperations(int group) { }
        public static void SetCurrentGroupName(string name) { }
    }

    public static class Selection
    {
        public static GameObject activeGameObject { get; set; }
        public static UnityEngine.Object activeObject { get; set; }
        public static GameObject[] gameObjects => new GameObject[0];
        public static UnityEngine.Object[] objects { get; set; }
    }

    public static class Handles
    {
        public static Vector2 GetMainGameViewSize() => new Vector2(1920f, 1080f);
    }

    public static class PrefabUtility
    {
        public static UnityEngine.Object InstantiatePrefab(UnityEngine.Object asset) => null;
        public static UnityEngine.Object InstantiatePrefab(UnityEngine.Object asset,
                                                           Transform parent) => null;
        // Into a SCENE rather than under a parent: that is how a setup tool puts a model
        // on a fighter without reparenting anything.
        public static UnityEngine.Object InstantiatePrefab(
            UnityEngine.Object asset, UnityEngine.SceneManagement.Scene scene) => null;
        public static GameObject GetOutermostPrefabInstanceRoot(UnityEngine.Object o) => null;
        public static T GetCorrespondingObjectFromOriginalSource<T>(T obj)
            where T : UnityEngine.Object => null;
        public static void RecordPrefabInstancePropertyModifications(UnityEngine.Object o) { }
        public static void UnpackPrefabInstance(GameObject instance,
                                                PrefabUnpackMode mode,
                                                InteractionMode action) { }
    }

    public enum PrefabUnpackMode { OutermostRoot, Completely }
    public enum InteractionMode { AutomatedAction, UserAction }

    public class AssetImporter : UnityEngine.Object
    {
        public string assetPath;
        public static AssetImporter GetAtPath(string path) => null;
        public void SaveAndReimport() { }
    }

    public class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType;
        public SpriteImportMode spriteImportMode;
        public TextureImporterNPOTScale npotScale;
        public bool mipmapEnabled;
        public FilterMode filterMode;
        public bool alphaIsTransparency;
        public bool sRGBTexture;
        public TextureImporterCompression textureCompression;
        public TextureWrapMode wrapMode;
        public float spritePixelsPerUnit;
        public Vector4 spriteBorder;
        public int maxTextureSize;
        public bool isReadable;
    }

    public enum TextureImporterType { Default, Sprite, NormalMap, GUI }
    public enum SpriteImportMode { None, Single, Multiple, Polygon }
    public enum TextureImporterNPOTScale { None, ToNearest, ToLarger, ToSmaller }
    public enum TextureImporterCompression { Uncompressed, Compressed, CompressedHQ, CompressedLQ }

    public class SerializedObject
    {
        public SerializedObject(UnityEngine.Object obj) { }
        public SerializedProperty FindProperty(string path) => null;
        public SerializedProperty GetIterator() => null;
        public void Update() { }
        public bool ApplyModifiedProperties() => false;
    }

    public class SerializedProperty
    {
        public string name;
        public string propertyPath;
        public SerializedPropertyType propertyType;
        public int arraySize;
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public string stringValue;
        public UnityEngine.Object objectReferenceValue;
        public Color colorValue;
        public SerializedProperty GetArrayElementAtIndex(int index) => null;
        public bool NextVisible(bool enterChildren) => false;
        public bool Next(bool enterChildren) => false;
    }

    public enum SerializedPropertyType
    {
        Generic, Integer, Boolean, Float, String, Color, ObjectReference, LayerMask,
        Enum, Vector2, Vector3, Vector4, Rect, ArraySize, Character, AnimationCurve,
        Bounds, Gradient, Quaternion, ExposedReference, FixedBufferSize, Vector2Int,
        Vector3Int, RectInt, BoundsInt, ManagedReference, Hash128,
    }
    /// <summary>Marks a static method to run when the editor loads the assemblies.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class InitializeOnLoadMethodAttribute : Attribute { }

    public enum PlayModeStateChange
    {
        EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode
    }

    public static class EditorApplication
    {
        public static Action<PlayModeStateChange> playModeStateChanged;
        public static bool isPlaying { get; set; }
    }

    public static class EditorPrefs
    {
        public static bool GetBool(string key, bool def = false) => def;
        public static void SetBool(string key, bool value) { }
        public static int GetInt(string key, int def = 0) => def;
        public static void SetInt(string key, int value) { }
        public static string GetString(string key, string def = "") => def;
        public static void SetString(string key, string value) { }
    }

    public static class Menu
    {
        public static void SetChecked(string path, bool on) { }
        public static bool GetChecked(string path) => false;
    }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) { }
        public static void MarkAllScenesDirty() { }
        public static bool SaveOpenScenes() => true;
    }

}

namespace UnityEditor.Events
{
    using UnityEngine.Events;

    /// <summary>
    /// Wiring a UnityEvent in the inspector, from code.
    ///
    /// The NuGet reference assemblies ALSO carry a UnityEngine.Events.UnityEventTools --
    /// the real editor does not -- so a file importing both namespaces is ambiguous here
    /// and nowhere else. The callers name this one with a using alias, which is valid in
    /// Unity too and says which of the two is meant.
    /// </summary>
    public static class UnityEventTools
    {
        public static void AddPersistentListener(UnityEvent ev, UnityAction call) { }
        public static void AddPersistentListener<T0>(UnityEvent<T0> ev, UnityAction<T0> call) { }
        public static void AddVoidPersistentListener(UnityEventBase ev, UnityAction call) { }
        public static void RemovePersistentListener(UnityEventBase ev, int index) { }
    }
}

namespace UnityEditor.Animations
{
    public class AnimatorController : RuntimeAnimatorController
    {
        public AnimatorControllerLayer[] layers { get; set; }
        public AnimatorControllerParameter[] parameters { get; set; }

        public static AnimatorController CreateAnimatorControllerAtPath(string path) => null;
        public void AddLayer(string name) { }
        public void AddLayer(AnimatorControllerLayer layer) { }
        public void AddParameter(string name, AnimatorControllerParameterType type) { }
        public void RemoveLayer(int index) { }
    }

    public class AnimatorControllerLayer
    {
        public string name { get; set; }
        public AnimatorStateMachine stateMachine { get; set; }
        public float defaultWeight { get; set; }
        public AvatarMask avatarMask { get; set; }
        public bool iKPass { get; set; }
        public AnimatorLayerBlendingMode blendingMode { get; set; }
    }

    public class AnimatorStateMachine : UnityEngine.Object
    {
        public ChildAnimatorState[] states { get; set; }
        public AnimatorState defaultState { get; set; }
        public AnimatorState AddState(string name) => null;
    }

    public class AnimatorState : UnityEngine.Object
    {
        public Motion motion { get; set; }
        public float speed { get; set; }
        public bool writeDefaultValues { get; set; }
    }

    public enum AnimatorLayerBlendingMode { Override, Additive }

    public struct ChildAnimatorState
    {
        public AnimatorState state;
        public Vector3 position;
    }
}

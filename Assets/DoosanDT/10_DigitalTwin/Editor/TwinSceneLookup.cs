// Editor-side helpers to find scene objects by hierarchy path (inactive objects included).
//
// GameObject.Find cannot see inactive objects, and several objects this batch must repair are
// inactive by design (the welcome page, the gaze interactor, the legacy canvases). So the tools
// walk the hierarchy explicitly. Paths are "/"-separated and start at the scene root.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    public static class TwinSceneLookup
    {
        public static Transform FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string[] segments = path.Split('/');
            Scene scene = SceneManager.GetActiveScene();
            Transform current = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == segments[0])
                {
                    current = root.transform;
                    break;
                }
            }

            if (current == null)
            {
                return null;
            }

            for (int i = 1; i < segments.Length; i++)
            {
                Transform next = null;
                for (int c = 0; c < current.childCount; c++)
                {
                    Transform child = current.GetChild(c);
                    if (child.name == segments[i])
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                {
                    return null;
                }

                current = next;
            }

            return current;
        }

        public static GameObject FindObject(string path)
        {
            Transform t = FindByPath(path);
            return t == null ? null : t.gameObject;
        }

        public static T FindComponent<T>(string path) where T : Component
        {
            GameObject go = FindObject(path);
            return go == null ? null : go.GetComponent<T>();
        }

        public static string PathOf(Transform t)
        {
            if (t == null)
            {
                return "<null>";
            }

            var parts = new System.Collections.Generic.List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        public static int RemoveMissingScripts(GameObject go)
        {
            if (go == null)
            {
                return 0;
            }

            int before = CountMissing(go);
            if (before == 0)
            {
                return 0;
            }

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            return before - CountMissing(go);
        }

        public static int CountMissing(GameObject go)
        {
            int count = 0;
            foreach (Component component in go.GetComponents<Component>())
            {
                if (component == null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
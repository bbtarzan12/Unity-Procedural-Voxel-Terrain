using System;
using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    protected static T instance;

    public static T Instance
    {
        get
        {
            if (instance != null) return instance;
            
            instance = (T) FindObjectOfType(typeof(T));

            if (instance == null)
            {
                instance = new GameObject(typeof(T).Name).AddComponent<T>();
            }

            DontDestroyOnLoad(instance);
            return instance;
        }
    }
}
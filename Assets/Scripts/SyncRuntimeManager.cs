using System;
using UnityEngine;

namespace jKnepel.UnitySync
{
    public class SyncRuntimeManager : MonoBehaviour
    {
        public event Action OnAwake;
        public event Action OnStart;
        public event Action OnUpdate;

        private void Awake()
        {
            OnAwake?.Invoke();
        }

        private void Start()
        {
            OnStart?.Invoke();
        }

        private void Update()
        {
            OnUpdate?.Invoke();
        }
    }
}

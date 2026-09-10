using System.Collections.Generic;
using UnityEngine;

namespace AvatarReplacer
{

    [DefaultExecutionOrder(100)]
    public class VrmMeshExpressionApplicator : MonoBehaviour
    {
        private VrmMeshBlendshapeAdapter _meshAdapter;
        private readonly List<(string key, float value)> _queue = new List<(string, float)>();

        public void Initialize(VrmMeshBlendshapeAdapter meshAdapter)
        {
            _meshAdapter = meshAdapter;
        }

        public void Queue(string standardKey, float value)
        {
            if (_meshAdapter == null || string.IsNullOrEmpty(standardKey)) return;
            _queue.Add((standardKey, value));
        }

        private void LateUpdate()
        {
            if (_meshAdapter == null) return;

            _meshAdapter.BeginFrame();

            foreach (var (key, value) in _queue)
            {
                _meshAdapter.ApplyWithStandardMapping(key, value);
            }
            _queue.Clear();

            _meshAdapter.EndFrame();
        }
    }
}

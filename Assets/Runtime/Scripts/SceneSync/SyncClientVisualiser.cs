using UnityEngine;

namespace jKnepel.UnitySync.SceneSync
{
    [System.Serializable]
    public class SyncClientVisualiser : MonoBehaviour
    {
        [SerializeField] private new Renderer renderer;
        [SerializeField] private TMPro.TMP_Text usernameObject;
        [SerializeField] private Material material;

        public void UpdateVisualiser(uint id, string username, Color color)
	    {
            name = $"{username}@{id}";
            usernameObject.text = username;
            usernameObject.color = color;
            if (material == null)
                material = new(renderer.sharedMaterial);
            material.SetColor("_Color", color);
            renderer.sharedMaterial = material;
        }
    }
}

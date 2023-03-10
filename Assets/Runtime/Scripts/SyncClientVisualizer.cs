using UnityEngine;

public class SyncClientVisualizer : MonoBehaviour
{
    [SerializeField] private new Renderer renderer;
    [SerializeField] private TMPro.TMP_Text usernameObject;
    private Material material;

    public void UpdateVisualizer(string sender, string username, Color color)
	{
        name = $"{username}@{sender}";
        usernameObject.text = username;
        usernameObject.color = color;
        if (material == null)
            material = new(renderer.sharedMaterial);
        material.SetColor("_Color", color);
        renderer.sharedMaterial = material;
    }
}

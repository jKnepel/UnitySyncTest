using UnityEngine;

public class SyncClientVisualizer : MonoBehaviour
{
    [SerializeField] private new Renderer renderer;
    [SerializeField] private TMPro.TMP_Text usernameObject;

    public void UpdateVisualizer(string sender, string username, Color color)
	{
        name = $"{username}@{sender}";
        usernameObject.text = username;
        usernameObject.color = color;
        renderer.sharedMaterial.SetColor("_Color", color);
    }
}

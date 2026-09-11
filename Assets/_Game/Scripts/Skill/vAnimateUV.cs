using UnityEngine;

/// <summary>
/// Scrolls UV texture offset on a Renderer's material at a given speed.
/// Instanced material is created automatically to avoid shared material mutation.
/// </summary>
public class vAnimateUV : MonoBehaviour
{
    [Tooltip("UV scroll speed per second (X = horizontal, Y = vertical)")]
    public Vector2 speed = new Vector2(0f, 0.5f);

    [Tooltip("Renderer to animate. Defaults to this GameObject's Renderer if left empty.")]
    public Renderer _renderer;

    [Tooltip("Shader texture property names to scroll (e.g. _MainTex)")]
    public string[] textureParameters = { "_MainTex" };

    private Material _instancedMaterial;
    private Vector2 _offset;

    private void Awake()
    {
        if (_renderer == null)
            _renderer = GetComponent<Renderer>();

        if (_renderer != null)
            _instancedMaterial = _renderer.material; // creates instance
    }

    private void Update()
    {
        if (_instancedMaterial == null || textureParameters == null)
            return;

        _offset += speed * Time.deltaTime;

        foreach (var param in textureParameters)
        {
            if (_instancedMaterial.HasProperty(param))
                _instancedMaterial.SetTextureOffset(param, _offset);
        }
    }

    private void OnDestroy()
    {
        if (_instancedMaterial != null)
            Destroy(_instancedMaterial);
    }
}

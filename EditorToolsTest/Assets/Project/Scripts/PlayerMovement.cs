using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    public float m_moveSpeed = 10f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current.aKey.isPressed)
        {
            transform.Translate(Vector2.left * m_moveSpeed * Time.deltaTime);
        }
        else if (Keyboard.current.dKey.isPressed)
        {
            transform.Translate(Vector2.right * m_moveSpeed * Time.deltaTime);
        }
    }
}

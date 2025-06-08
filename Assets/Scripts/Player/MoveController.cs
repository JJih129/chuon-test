using UnityEngine;

public class MoveController : MonoBehaviour
{
    public float speed = 5f;

    public void MoveForward()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    public void MoveBackward()
    {
        transform.Translate(Vector3.back * speed * Time.deltaTime);
    }

    public void MoveLeft()
    {
        transform.Translate(Vector3.left * speed * Time.deltaTime);
    }

    public void MoveRight()
    {
        transform.Translate(Vector3.right * speed * Time.deltaTime);
    }
}

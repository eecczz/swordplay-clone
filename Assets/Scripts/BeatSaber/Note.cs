using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Note : MonoBehaviour
{
    int r;
    public Transform direction;
    private void Start()
    {
        Invoke("DestroyNote", 10f);
        r = Random.Range(0, 4);
        if (r == 0)
        {
            direction.localRotation = Quaternion.Euler(new Vector3(0, 0, 0));
        }
        if (r == 1)
        {
            direction.localRotation = Quaternion.Euler(new Vector3(0, 0, 90));
        }
        if (r == 2)
        {
            direction.localRotation = Quaternion.Euler(new Vector3(0, 0, -90));
        }
        if (r == 3)
        {
            direction.localRotation = Quaternion.Euler(new Vector3(0, 0, 180));
        }
    }
    private void Update()
    {
        transform.Translate(Vector3.forward * 10 * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (gameObject.layer == 6 && other.gameObject.layer == 8)
        {

        }
        if (gameObject.layer == 7 && other.gameObject.layer == 9)
        {

        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (gameObject.layer == 6 && other.gameObject.layer == 8)
        {
            Destroy(gameObject);
        }
        if (gameObject.layer == 7 && other.gameObject.layer == 9)
        {
            Destroy(gameObject);
        }
    }

    void DestroyNote()
    {
        Destroy(gameObject);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyHitVfx : MonoBehaviour
{
    void Start()
    {
        if(PlayerWorker.ent && EnemyWorker.psword)
        {
            transform.rotation = Quaternion.LookRotation(new Vector3(-Camera.main.transform.forward.x, 0, -Camera.main.transform.forward.z));
            Vector3 v1 = EnemyWorker.psword.GetComponent<Rigidbody>().linearVelocity;
            v1 = new Vector3(v1.x, v1.y, 0);
            float r1 = Mathf.Atan2(v1.y, v1.x) * Mathf.Rad2Deg;
            transform.RotateAround(transform.position, transform.forward, r1);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SaberMotion : MonoBehaviour
{
    public Transform jointl, jointr;
    public float sensitivity = 5f;
    float tx, ty, tx1, ty1;

    private void Start()
    {
        Cursor.visible = false;
    }

    void Update()
    {
        //pc
        //sword movement
        tx += Input.GetAxis("Mouse X") * sensitivity;
        ty += Input.GetAxis("Mouse Y") * sensitivity;
        tx = Mathf.Clamp(tx, -130, 60);
        ty = Mathf.Clamp(ty, -45, 90);
        jointr.localRotation = Quaternion.Euler(new Vector3(0, 90 + tx, 90 - ty));
        jointr.RotateAround(jointr.position, jointr.forward, Mathf.Clamp(ty * -1/5, -90, 90));
        //sword1 movement
        tx1 -= Input.GetAxis("Mouse X") * sensitivity; //negative adding is an option
        ty1 += Input.GetAxis("Mouse Y") * sensitivity;
        tx1 = Mathf.Clamp(tx1, -60, 130);
        ty1 = Mathf.Clamp(ty1, -45, 90);
        jointl.localRotation = Quaternion.Euler(new Vector3(180, 90 + tx1, 90 + ty1));
        jointl.RotateAround(jointl.position, jointl.forward, Mathf.Clamp(ty1 * 1 / 5, -90, 90));

        //mobile
        if (Input.touchCount > 0 && Input.touches[0].position.x > 960)
        {
            //sword movement
            tx += Input.touches[0].deltaPosition.x * sensitivity;
            ty += Input.touches[0].deltaPosition.y * sensitivity;
            tx = Mathf.Clamp(tx, -130, 60);
            ty = Mathf.Clamp(ty, -45, 90);
            jointr.localRotation = Quaternion.Euler(new Vector3(0, 90 + tx, 90 - ty));
            jointr.RotateAround(jointr.position, jointr.forward, Mathf.Clamp(ty * -1 / 5, -90, 90));
            if (Input.touchCount > 1)
            {
                //sword1 movement
                tx1 += Input.touches[1].deltaPosition.x * sensitivity; //negative adding is an option
                ty1 += Input.touches[1].deltaPosition.y * sensitivity;
                tx1 = Mathf.Clamp(tx1, -60, 130);
                ty1 = Mathf.Clamp(ty1, -45, 90);
                jointl.localRotation = Quaternion.Euler(new Vector3(180, 90 + tx1, 90 + ty1));
                jointl.RotateAround(jointl.position, jointl.forward, Mathf.Clamp(ty1 * 1 / 5, -90, 90));
            }
        }
        else if (Input.touchCount > 0 && Input.touches[0].position.x <= 960)
        {
            if (Input.touchCount > 1)
            {
                //sword movement
                tx += Input.touches[1].deltaPosition.x * sensitivity;
                ty += Input.touches[1].deltaPosition.y * sensitivity;
                tx = Mathf.Clamp(tx, -130, 60);
                ty = Mathf.Clamp(ty, -45, 90);
                jointr.localRotation = Quaternion.Euler(new Vector3(0, 90 + tx, 90 - ty));
                jointr.RotateAround(jointr.position, jointr.forward, Mathf.Clamp(ty * -1 / 5, -90, 90));
            }
            //sword1 movement
            tx1 += Input.touches[0].deltaPosition.x * sensitivity; //negative adding is an option
            ty1 += Input.touches[0].deltaPosition.y * sensitivity;
            tx1 = Mathf.Clamp(tx1, -60, 130);
            ty1 = Mathf.Clamp(ty1, -45, 90);
            jointl.localRotation = Quaternion.Euler(new Vector3(180, 90 + tx1, 90 + ty1));
            jointl.RotateAround(jointl.position, jointl.forward, Mathf.Clamp(ty1 * 1 / 5, -90, 90));
        }
    }
}

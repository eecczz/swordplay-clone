using FIMSpace.FProceduralAnimation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Balance : MonoBehaviour
{
    public Transform body;
    private void Update()
    {
        transform.position = body.position;
    }
}

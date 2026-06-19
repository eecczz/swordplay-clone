using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DissolveSphere : MonoBehaviour {

    Material[] mat;
    float time;
    private void Start() {
        mat = GetComponent<Renderer>().materials;
        time = Time.time;
    }

    private void Update() {
        for (int i = 0; i < GetComponent<Renderer>().materials.Length; i++)
            mat[i].SetFloat("_Dissolve", Mathf.Sin(Time.time - time) / 2 + 0.5f);
    }
}
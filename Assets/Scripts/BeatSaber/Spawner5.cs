using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spawner5 : MonoBehaviour
{
    public GameObject note_b;
    public GameObject note_r;
    float cool;
    // Update is called once per frame
    void Start()
    {
        cool = Random.Range(0, 500);
    }
    private void Update()
    {
        if(cool>0)
        {
            cool--;
        }
        if(cool==0)
        {
            cool = Random.Range(500, 1000);
            int r = Random.Range(0,2);
            if(r==0)
                Instantiate(note_b, transform.position, transform.rotation);
            if(r==1)
                Instantiate(note_r, transform.position, transform.rotation);
        }
    }
}
